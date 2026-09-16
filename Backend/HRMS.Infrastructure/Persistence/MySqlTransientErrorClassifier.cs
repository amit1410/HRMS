using HRMS.Application.Abstractions;
using MySql.Data.MySqlClient;

namespace HRMS.Infrastructure.Persistence;

/// <summary>Classifies only MySQL deadlocks for the existing whole-operation retry boundary.</summary>
public sealed class MySqlTransientErrorClassifier : ILeaveRequestSubmissionDeadlockClassifier, IDatabaseTransientErrorClassifier
{
    public bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException mySqlException && mySqlException.Number is 1213 or 40001)
                return true;
        }

        return false;
    }
}
