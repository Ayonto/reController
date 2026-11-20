namespace ReController.Platform.Windows.Input;

public enum DeviceFamily
{
    Xbox,
    PlayStation,
    Nintendo,
    Generic,
}

/// <param name="Key">Stable identifier used to remember per-device assignments.</param>
public sealed record PhysicalDeviceInfo(
    string Key,
    string Name,
    DeviceFamily Family,
    string Provider,
    ushort VendorId,
    ushort ProductId,
    bool SupportsExtendedButtons);

internal interface IInputProvider : IDisposable
{
    string Name { get; }

    /// <summary>False when the backend failed to initialise (e.g. missing DLL); it is then skipped.</summary>
    bool IsAvailable { get; }

    void Initialize();

    /// <summary>Called once per engine tick before any <see cref="TryRead"/>.</summary>
    void Update();

    /// <summary>Called a few times per second to detect hot-plugging. Must be cheap.</summary>
    void Enumerate(List<PhysicalDeviceInfo> into);

    bool TryRead(string key, out ReController.Core.Input.GamepadState state);

    /// <summary>Request rumble; 0..1 per motor. Applied on the next <see cref="Update"/>.</summary>
    void SetRumble(string key, float low, float high);
}
