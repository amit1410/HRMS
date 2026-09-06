using HRMS.Application.Abstractions;
using Microsoft.Data.SqlClient;

namespace HRMS.Infrastructure.Persistence;

public sealed class SqlServerLeaveRequestSubmissionDeadlockClassifier : ILeaveRequestSubmissionDeadlockClassifier, IDatabaseTransientErrorClassifier
{
    public bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number == 1205)
                return true;
        }

        return false;
    }
}
