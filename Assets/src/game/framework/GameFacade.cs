
using PureMVC.Patterns;


public class GameFacade : Facade
{
    protected override void InitializeController()
    {
        base.InitializeController();
        RegisterCommand(FrameworkCmdDef.STARTUP, typeof(StartupCommand));

    }

    public void startup()
    {
        //SendNotification()
    }
}
