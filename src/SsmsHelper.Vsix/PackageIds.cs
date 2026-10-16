using System;

namespace SsmsHelper;

/// <summary>GUIDs and command IDs. Must match the symbols in SsmsHelperPackage.vsct.</summary>
internal static class PackageIds
{
    public const string PackageGuidString = "4756aba8-bf31-468b-a64f-1fb39e6e36cc";
    public const string CommandSetGuidString = "ee6ca84e-96fa-481e-b475-d121aaccdeee";

    public static readonly Guid CommandSet = new(CommandSetGuidString);

    public const int AboutCommandId = 0x0100;
    public const int RefreshSchemaCommandId = 0x0101;
}
