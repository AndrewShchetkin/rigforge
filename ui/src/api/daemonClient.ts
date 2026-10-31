import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
  type ILogger,
  type IRetryPolicy,
} from '@microsoft/signalr';
import { HUB_PATH, type DaemonHubEvents, type DaemonHubMethods, type PreviewChannel } from './contracts.ts';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

type HubMethod = keyof DaemonHubMethods;
type HubEvent = keyof DaemonHubEvents;
type HubResult<K extends HubMethod> = Awaited<ReturnType<DaemonHubMethods[K]>>;

const RETRY_DELAYS_MS = [0, 1_000, 2_000, 5_000];
const STEADY_RETRY_DELAY_MS = 5_000;

/** 0 s, 1 s, 2 s, 5 s, then every 5 s, forever. */
function retryDelayMs(previousRetryCount: number): number {
  return RETRY_DELAYS_MS[previousRetryCount] ?? STEADY_RETRY_DELAY_MS;
}

const retryForever: IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelayMs(previousRetryCount),
};

/**
 * Typed wrapper around the hub connection. Keeps (re)connecting until the daemon answers and restores
 * preview subscriptions after every reconnect, because the daemon keeps them per connection.
 */
export class DaemonClient {
  readonly #connection: HubConnection;
  readonly #previews = new Set<PreviewChannel>();
  readonly #statusListeners = new Set<(status: ConnectionStatus) => void>();
  readonly #connectedListeners = new Set<() => void>();
  #status: ConnectionStatus = 'disconnected';
  #wanted = false;
  #connecting = false;
  #stopping: Promise<void> = Promise.resolve();
  #cancelRetryWait: (() => void) | null = null;

  constructor(url: string = HUB_PATH) {
    this.#connection = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect(retryForever)
      .configureLogging(this.#createLogger())
      .build();
    this.#connection.onreconnecting(() => this.#setStatus('reconnecting'));
    this.#connection.onreconnected(() => this.#handleConnected());
    // Also fires when no automatic reconnect is attempted (e.g. the server closed the connection); start over.
    this.#connection.onclose(() => {
      this.#setStatus('disconnected');
      if (this.#wanted) {
        this.#ensureConnectLoop();
      }
    });
  }

  /** Connects, retrying forever until the daemon appears. Idempotent. */
  start(): void {
    if (this.#wanted) {
      return;
    }
    this.#wanted = true;
    this.#ensureConnectLoop();
  }

  async stop(): Promise<void> {
    this.#wanted = false;
    this.#cancelRetryWait?.();
    this.#stopping = this.#connection.stop().catch((error: unknown) => {
      console.warn('Stopping the hub connection failed', error);
    });
    await this.#stopping;
    if (!this.#wanted) {
      this.#setStatus('disconnected');
    }
  }

  invoke<K extends HubMethod>(method: K, ...args: Parameters<DaemonHubMethods[K]>): Promise<HubResult<K>> {
    return this.#connection.invoke<HubResult<K>>(method, ...args);
  }

  /** Subscribes to a server event and returns the unsubscribe function. */
  on<K extends HubEvent>(event: K, handler: DaemonHubEvents[K]): () => void {
    this.#connection.on(event, handler);
    return () => {
      this.#connection.off(event, handler);
    };
  }

  onStatusChange(listener: (status: ConnectionStatus) => void): () => void {
    this.#statusListeners.add(listener);
    return () => {
      this.#statusListeners.delete(listener);
    };
  }

  /** Runs after every successful connect and reconnect. */
  onConnected(listener: () => void): () => void {
    this.#connectedListeners.add(listener);
    return () => {
      this.#connectedListeners.delete(listener);
    };
  }

  /** Remembers the subscription (re-sent after reconnects) and forwards it when connected. */
  async setPreview(channel: PreviewChannel, enabled: boolean): Promise<void> {
    if (enabled) {
      this.#previews.add(channel);
    } else {
      this.#previews.delete(channel);
    }
    if (this.#connection.state === HubConnectionState.Connected) {
      await this.invoke('SetPreview', channel, enabled);
    }
  }

  #ensureConnectLoop(): void {
    if (this.#connecting) {
      return;
    }
    this.#connecting = true;
    void this.#runConnectLoop().finally(() => {
      this.#connecting = false;
    });
  }

  async #runConnectLoop(): Promise<void> {
    for (let attempt = 0; this.#wanted; attempt++) {
      await this.#stopping;
      if (!this.#wanted || this.#connection.state !== HubConnectionState.Disconnected) {
        return;
      }
      if (attempt === 0) {
        this.#setStatus('connecting');
      }
      try {
        await this.#connection.start();
      } catch {
        if (this.#wanted) {
          if (attempt === 0) {
            console.info('RigForge daemon is not reachable yet; retrying in the background.');
          }
          this.#setStatus('disconnected');
          await this.#waitBeforeRetry(retryDelayMs(attempt));
        }
        continue;
      }
      if (this.#wanted) {
        this.#handleConnected();
      }
      return;
    }
  }

  /** SignalR logs every failed connection attempt as an error; while waiting for the daemon that is just noise. */
  #createLogger(): ILogger {
    return {
      log: (level, message) => {
        if (level < LogLevel.Warning || (level >= LogLevel.Error && this.#status !== 'connected')) {
          return;
        }
        if (level === LogLevel.Warning) {
          console.warn(`[SignalR] ${message}`);
        } else {
          console.error(`[SignalR] ${message}`);
        }
      },
    };
  }

  #waitBeforeRetry(delayMs: number): Promise<void> {
    return new Promise((resolve) => {
      const finish = () => {
        clearTimeout(timer);
        this.#cancelRetryWait = null;
        resolve();
      };
      const timer = setTimeout(finish, delayMs);
      this.#cancelRetryWait = finish;
    });
  }

  #handleConnected(): void {
    this.#setStatus('connected');
    for (const channel of this.#previews) {
      this.invoke('SetPreview', channel, true).catch((error: unknown) => {
        console.warn(`Could not restore the ${channel} preview subscription`, error);
      });
    }
    for (const listener of this.#connectedListeners) {
      listener();
    }
  }

  #setStatus(status: ConnectionStatus): void {
    if (status === this.#status) {
      return;
    }
    this.#status = status;
    for (const listener of this.#statusListeners) {
      listener(status);
    }
  }
}
