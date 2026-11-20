using System.Runtime.InteropServices;

namespace ReController.Platform.Windows.Native;

internal static partial class XInputNative
{
    public const int MaxControllers = 4;
    public const int ErrorSuccess = 0;
    public const int ErrorDeviceNotConnected = 1167;

    public const ushort DPadUp = 0x0001;
    public const ushort DPadDown = 0x0002;
    public const ushort DPadLeft = 0x0004;
    public const ushort DPadRight = 0x0008;
    public const ushort Start = 0x0010;
    public const ushort Back = 0x0020;
    public const ushort LeftThumb = 0x0040;
    public const ushort RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100;
    public const ushort RightShoulder = 0x0200;
    public const ushort Guide = 0x0400;
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    // Ordinal 100 = XInputGetStateEx: identical to XInputGetState but also reports the Guide button.
    [LibraryImport("xinput1_4.dll", EntryPoint = "#100")]
    private static partial int XInputGetStateEx(int userIndex, out XInputState state);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial int XInputGetStatePublic(int userIndex, out XInputState state);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    public static partial int XInputSetState(int userIndex, ref XInputVibration vibration);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")]
    public static partial int XInputGetCapabilities(int userIndex, int flags, out XInputCapabilities caps);

    private static bool _exUnavailable;

    public static int GetState(int userIndex, out XInputState state)
    {
        if (!_exUnavailable)
        {
            try { return XInputGetStateEx(userIndex, out state); }
            catch (EntryPointNotFoundException) { _exUnavailable = true; }
        }
        return XInputGetStatePublic(userIndex, out state);
    }
}
