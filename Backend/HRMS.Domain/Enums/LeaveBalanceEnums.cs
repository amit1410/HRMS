namespace HRMS.Domain.Enums;

public enum LeaveBalanceTransactionType
{
    Opening = 0,
    Accrual = 1,
    ExternalGrant = 2,
    Reservation = 3,
    ReservationRelease = 4,
    Consumption = 5,
    CancellationRestore = 6
}

public enum LeaveBalanceSourceType
{
    Policy = 0,
    External = 1
}

public enum LeaveBalanceActorType
{
    System = 0,
    User = 1,
    External = 2
}
