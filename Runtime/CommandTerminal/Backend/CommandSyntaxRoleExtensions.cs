namespace WallstopStudios.DxCommandTerminal.Backend
{
    using CommandSyntaxRole = CommandSyntaxClassifier.CommandSyntaxRole;

    internal static class CommandSyntaxRoleExtensions
    {
        internal const int RoleSlotCount = (int)CommandSyntaxRole.Invalid + 1;

        internal static int ToIndex(this CommandSyntaxRole role)
        {
            return (int)role;
        }
    }
}
