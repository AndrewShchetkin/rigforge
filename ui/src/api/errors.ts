const INVOKE_PREFIX = /^An unexpected error occurred invoking '[^']*' on the server\.\s*/;
const EXCEPTION_PREFIX = /^[A-Za-z_][\w.]*Exception:\s*/;
const NOT_CONNECTED = /not in the 'Connected' State/i;

/** Extracts the message the daemon put into its HubException from a SignalR invocation error. */
export function describeHubError(error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error);
  if (NOT_CONNECTED.test(raw)) {
    return 'Not connected to the daemon.';
  }
  const message = raw.replace(INVOKE_PREFIX, '').replace(EXCEPTION_PREFIX, '').trim();
  return message.length > 0 ? message : 'The daemon rejected the request.';
}
