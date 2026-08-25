namespace LabDesktop.Client.App.Security;

internal interface ISshCredentialStore
{
    string? Read(string routeIdentity);

    void Write(string routeIdentity, string userName, string password);

    void Delete(string routeIdentity);
}
