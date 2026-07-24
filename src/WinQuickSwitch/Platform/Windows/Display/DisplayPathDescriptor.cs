namespace WinQuickSwitch.Platform.Windows.Display;

internal sealed record DisplayPathDescriptor(
    long AdapterId,
    uint SourceId,
    uint TargetId,
    DisplayOutputTechnology OutputTechnology,
    bool IsActive,
    bool IsAvailable);

internal enum DisplayOutputTechnology : uint
{
    Other = 0xFFFFFFFF,
    Hd15 = 0,
    SVideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    DisplayPortUsbTunnel = 18,
    Internal = 0x80000000,
}
