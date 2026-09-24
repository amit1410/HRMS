namespace HRMS.Application.Abstractions;

public interface ISeparationExitFailureInjector
{
    void BeforeEmploymentExit();
    void BeforeAccessDeprovision();
    void BeforeSessionRevocation();
    void BeforeRoleReconciliation();
    void BeforeFinalClosure();
}
