using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

static void Dump(string title, PipeSecurity sec)
{
    Console.WriteLine(title);
    try
    {
        foreach (PipeAccessRule r in sec.GetAccessRules(true, true, typeof(NTAccount)))
            Console.WriteLine($"  {r.IdentityReference} | {r.AccessControlType} | {r.PipeAccessRights}");
    }
    catch (Exception e)
    {
        Console.WriteLine("  dump_err=" + e.GetType().Name + ": " + e.Message);
    }
}

Console.WriteLine("user=" + WindowsIdentity.GetCurrent().Name);

try
{
    using var server = new NamedPipeServerStream("zivai_sec_probe_default", PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.None);
    var def = server.GetAccessControl();
    Dump("DEFAULT_ACL:", def);
}
catch (Exception e)
{
    Console.WriteLine("DEFAULT_FAIL " + e.GetType().Name + ": " + e.Message);
}

try
{
    var ps = new PipeSecurity();
    var me = WindowsIdentity.GetCurrent().User!;
    ps.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
    ps.SetAccessRuleProtection(true, false);
    using var server2 = NamedPipeServerStreamAcl.Create("zivai_sec_probe_restricted", PipeDirection.InOut,
        1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, ps);
    var sec2 = server2.GetAccessControl();
    Dump("RESTRICTED_ACL (current user only):", sec2);
    Console.WriteLine("RESTRICT_METHOD=PipeSecurity + SetAccessRuleProtection(true,false) + NamedPipeServerStreamAcl.Create");
}
catch (Exception e)
{
    Console.WriteLine("RESTRICT_FAIL " + e.GetType().Name + ": " + e.Message);
}

Console.WriteLine("DONE");
