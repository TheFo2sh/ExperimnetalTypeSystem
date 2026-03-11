using Shouldly;
using Xunit;

namespace ExperimnetalTypeSystem;

[ExpermientalTyping]
public partial class Consumer
{
    public UnionType CurrentUser => typeof(User) | typeof(Profile);
    public UnionType XUser => typeof(User) | typeof(Profile) | typeof(Profile2)|typeof(Profile3);

    [Fact]
    public void Test()
    {
        CurrentUserType current = new User("First", "Last", "Email");
        current.User.ShouldBeEquivalentTo(new User("First", "Last", "Email"));
        current.Profile.ShouldBeNull();
        current.FirstName.ShouldBe("First");
        current.LastName.ShouldBe("Last");
    }

    [Fact]
    private static void Test2()
    {
        XUserType xuser = new User("First", "Last", "Email");
        xuser.FirstName.ShouldBe("First");
    }
}