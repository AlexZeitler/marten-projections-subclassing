using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Xunit.Abstractions;
using static MartenDotNetTestTemplate.Tests.TestDatabase;

namespace MartenDotNetTestTemplate.Tests.UserLifecycleTestMultiSessionClassBasedProjectionTypes;

public abstract record User(
  Guid Id,
  string Email,
  string Name
);

public record InvitedUser(
  Guid Id,
  string Email,
  string Name,
  DateTimeOffset InvitedOn
) : User(
  Id,
  Email,
  Name
);

public record UserInvited(
  Guid Id,
  string Email,
  string Name,
  DateTimeOffset InvitedOn
);

public record ActiveUser(
  Guid Id,
  string Email,
  string Name,
  DateTimeOffset ActivatedOn
) : User(Id, Email, Name);

public record InvitationAccepted(
  Guid Id,
  string Email,
  string Name,
  DateTimeOffset AcceptedOn
);

public class InvitedUserProjection : SingleStreamProjection<InvitedUser>
{
  public InvitedUserProjection()
  {
    DeleteEvent<InvitationAccepted>();
  }

  public InvitedUser Create(
    UserInvited @event
  )
  {
    return new InvitedUser(
      @event.Id,
      @event.Email,
      @event.Name,
      @event.InvitedOn
    );
  }
}

public class ActiveUserProjection : SingleStreamProjection<ActiveUser>
{
  public ActiveUser Create(
    InvitationAccepted @event
  )
  {
    return new ActiveUser(
      @event.Id,
      @event.Email,
      @event.Name,
      @event.AcceptedOn
    );
  }
}

public class When_user_is_invited_and_accepting : IAsyncLifetime
{
  private readonly ITestOutputHelper _testOutputHelper;
  private DocumentStore? _store;
  private ActiveUser? _activeUser;
  private InvitedUser? _invitedUser;

  public When_user_is_invited_and_accepting(
    ITestOutputHelper testOutputHelper
  ) => _testOutputHelper = testOutputHelper;

  public async Task InitializeAsync()
  {
    _store = DocumentStore.For(
      _ =>
      {
        _.Schema
          .For<User>()
          .AddSubClass<InvitedUser>()
          .AddSubClass<ActiveUser>();

        _.Connection(GetTestDbConnectionString);
        _.Projections.Add<InvitedUserProjection>(ProjectionLifecycle.Inline);
        _.Projections.Add<ActiveUserProjection>(ProjectionLifecycle.Inline);
      }
    );

    var invited = new UserInvited(
      Guid.NewGuid(),
      "jane@acme.inc",
      "Jane",
      DateTimeOffset.Now
    );

    var accepted = new InvitationAccepted(
      invited.Id,
      invited.Email,
      invited.Name,
      DateTimeOffset.Now
    );

    await using var inviteSession = _store.LightweightSession();
    inviteSession.Events.Append(
      invited.Id,
      invited
    );

    await inviteSession.SaveChangesAsync();

    await using var activeSession = _store.LightweightSession();
    activeSession.Events.Append(invited.Id, accepted);
    await activeSession.SaveChangesAsync();

    _invitedUser = activeSession.Load<InvitedUser>(invited.Id);
    _activeUser = activeSession.Load<ActiveUser>(invited.Id);
  }

  [Fact]
  public void should_remove_invited_user() => _invitedUser.ShouldBeNull();

  [Fact]
  public void should_create_active_user()
  {
    _activeUser.ShouldNotBeNull();
    _activeUser.Name.ShouldBe("Jane");
  }

  public Task DisposeAsync() => Task.CompletedTask;
}
