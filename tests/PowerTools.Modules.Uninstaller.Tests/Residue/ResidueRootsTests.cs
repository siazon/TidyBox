using PowerTools.Modules.Uninstaller.Residue;

namespace PowerTools.Modules.Uninstaller.Tests.Residue;

public class ResidueRootsTests
{
    [Fact]
    public void Real_roots_include_the_native_program_files_folder_even_for_a_32_bit_process()
    {
        var native = Environment.GetEnvironmentVariable("ProgramW6432");
        if (native is null) return; // 32-bit Windows: there is only one Program Files

        var roots = ResidueRoots.Real();

        Assert.Contains(native, roots.FolderRoots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(native, roots.ElevatedFolderRoots, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Real_roots_treat_the_user_profile_folders_as_not_needing_elevation()
    {
        var roots = ResidueRoots.Real();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Contains(local, roots.FolderRoots, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(local, roots.ElevatedFolderRoots, StringComparer.OrdinalIgnoreCase);
    }
}
