namespace HRMS.Application.Services;

public static class PayrollRoundingPolicy
{
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
