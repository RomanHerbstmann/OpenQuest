using OpenQuest.Core.Adapters;

namespace OpenQuest.Core.Tests;

public class SourceSnapshotTests
{
    [Fact]
    public void Schema_hash_ignores_field_order_but_notices_added_or_removed_fields()
    {
        var a = SourceSnapshot.Hash(["str_schl", "baumgruppe"]);
        Assert.Equal(a, SourceSnapshot.Hash(["baumgruppe", "str_schl"]));
        Assert.NotEqual(a, SourceSnapshot.Hash(["baumgruppe", "str_schl", "hoehe"]));
        Assert.NotEqual(a, SourceSnapshot.Hash(["baumgruppe"]));
    }
}
