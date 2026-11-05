using Wasmtime;

namespace RigForge.Infrastructure.CrossPlatform.Effects;

/// <summary>Store limits shared by catalog validation and a running effect. No WASI configuration is applied.</summary>
internal static class WasmEffectHost
{
    public static Engine CreateEngine() => new(new Config().WithFuelConsumption(true));

    public static Store CreateStore(Engine engine)
    {
        var store = new Store(engine);
        store.SetLimits(
            memorySize: WasmGuestLayout.MaxMemoryBytes,
            memories: 1,
            instances: 1);
        return store;
    }
}
