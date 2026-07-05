using ReflowOven.Application.Common;
using Xunit;

namespace ReflowOven.Tests;

public class DefaultsTests
{
    [Fact]
    public void Catalog_has_the_fifty_built_in_programs()
    {
        Assert.Equal(50, Defaults.CatalogPrograms().Count); // 6 base + 44 generated
    }

    [Fact]
    public void Reference_data_matches_the_frontend()
    {
        Assert.Equal(10, Defaults.FaultTypes().Count); // 8 original + E-170/E-180 (firmware BOARD_OVER_TEMP/PRECHARGE)
        Assert.Equal(11, Defaults.NotificationSettings().Count);
        Assert.Equal(7, Defaults.RunSeries().Count);
    }

    [Fact]
    public void Notifications_carry_a_sequential_display_order()
    {
        var notifications = Defaults.NotificationSettings();
        Assert.Equal(0, notifications[0].Order);
        Assert.Equal(notifications.Count - 1, notifications[^1].Order);
    }

    [Fact]
    public void Factory_program_starts_at_baseline()
    {
        var program = Defaults.FactoryProgram();
        Assert.Equal(Defaults.FactoryProgramId, program.Id);
        Assert.Equal(0, program.Profile[0].T);
        Assert.Equal(0, program.Profile[0].Temp); // baseline start (was 25 ambient)
    }
}
