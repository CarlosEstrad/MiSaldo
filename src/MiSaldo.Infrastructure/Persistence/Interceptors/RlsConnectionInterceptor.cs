using System.Data.Common;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MiSaldo.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Sets the PostgreSQL session variable <c>app.user_id</c> right after a
/// connection opens, so the Row Level Security policies can restrict every row
/// to the current user (second isolation barrier, RNF-SEG-04). The value comes
/// from <see cref="ICurrentUser"/>; when there is no user, the variable is set
/// to empty, which the policies treat as "no rows".
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentUser _currentUser;

    public RlsConnectionInterceptor(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public override void ConnectionOpened(
        DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyUserId(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyUserIdAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyUserId(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        ConfigureCommand(command);
        command.ExecuteNonQuery();
    }

    private async Task ApplyUserIdAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        ConfigureCommand(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Uses set_config with a parameter to avoid any string interpolation into
    // SQL. The third argument (false) makes the setting last for the session.
    private void ConfigureCommand(DbCommand command)
    {
        command.CommandText = "SELECT set_config('app.user_id', @user_id, false);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "user_id";
        parameter.Value = _currentUser.UserId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
    }
}
