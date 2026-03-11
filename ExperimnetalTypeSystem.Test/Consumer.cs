using Shouldly;
using Xunit;

namespace ExperimnetalTypeSystem;

[ExpermientalTyping]
public partial class Consumer
{
    public UnionType CurrentUser => typeof(User) | typeof(Profile);

    [Fact]
    public void Test()
    {
        CurrentUserType current = new User("First", "Last", "Email");
        current.User.ShouldBeEquivalentTo(new User("First", "Last", "Email"));
        current.Profile.ShouldBeNull();
        current.FirstName.ShouldBe("First");
        current.LastName.ShouldBe("Last");
        
    }
}