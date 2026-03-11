using Shouldly;
using Xunit;

namespace ExperimnetalTypeSystem;

[ExpermientalTyping]
public partial class Consumer
{
    public UnionType CurrentUser => typeof(User) | typeof(Profile);
    public UnionType XUser => typeof(User) | typeof(Profile) | typeof(Profile2)|typeof(Profile3);
    public UnionType LUser => typeof(List<User>) | typeof(List<Profile>) | typeof(List<Profile2>)|typeof(List<Profile3>);

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

    [Fact]
    public static void Test3()
    {
        CurrentUserType current = new User("First", "Last", "Email");
        current.GetValue().ShouldBeEquivalentTo(new User("First", "Last", "Email"));
    }

    [Fact]
    public static void TestExhaustiveSwitch()
    {
        CurrentUserType current = new User("First", "Last", "Email");

        // This is exhaustive - handles both User and Profile
        var result = current.GetValue() switch
        {
            User u => $"User: {u.FirstName}",
            Profile p => $"Profile: {p.FirstName}",
            _ => throw new InvalidOperationException()
        };

        result.ShouldBe("User: First");
    }

    [Fact]
    public static void TestNonExhaustiveSwitch()
    {
        CurrentUserType current = new User("First", "Last", "Email");

        // This is NON-exhaustive - missing Profile!
        // Analyzer should warn: ONEOF001
        var result = current.GetValue() switch
        {
            User u => $"User: {u.FirstName}",
            Profile profile => $"profile {profile.FirstName}",
            _ => "Unknown"
        };

        result.ShouldBe("User: First");
    }

    [Fact]
    public static void TestExhaustiveSwitchStatement()
    {
        XUserType xuser = new User("First", "Last", "Email");
        string result;

        // Exhaustive switch statement
        switch (xuser.GetValue())
        {
            case User u:
                result = $"User: {u.FirstName}";
                break;
            case Profile p:
                result = $"Profile: {p.FirstName}";
                break;
            case Profile2 p2:
                result = $"Profile2: {p2.FirstName}";
                break;
            case Profile3 p3:
                result = $"Profile3: {p3.FirstName}";
                break;
            default:
                throw new InvalidOperationException();
        }

        result.ShouldBe("User: First");
    }
    
    [Fact]
    public static void TestExhaustiveSwitchStatement_missing_cases()
    {
        XUserType xuser = new User("First", "Last", "Email");
        string result = "";

        // Non-exhaustive switch statement - missing Profile3!
        // Analyzer should warn: ONEOF001
        switch (xuser.GetValue())
        {
            case User u:
                result = $"User: {u.FirstName}";
                break;
            case Profile p:
                result = $"Profile: {p.FirstName}";
                break;
            case Profile2 p2:
                result = $"Profile2: {p2.FirstName}";
                break;
            case Profile3 profile3:
                throw new NotImplementedException();
        }

        result.ShouldBe("User: First");
    }
}