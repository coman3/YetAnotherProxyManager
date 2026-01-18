using LiteDB;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Data;

public class LiteDbRepository : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<ProxyRoute> _routes;
    private readonly ILiteCollection<Certificate> _certificates;
    private readonly ILiteCollection<BsonDocument> _settings;

    public LiteDbRepository(string dataPath)
    {
        var dbPath = Path.Combine(dataPath, "proxymanager.db");
        Directory.CreateDirectory(dataPath);
        _database = new LiteDatabase(dbPath);

        _routes = _database.GetCollection<ProxyRoute>("routes");
        _routes.EnsureIndex(x => x.Name);
        _routes.EnsureIndex(x => x.Enabled);

        _certificates = _database.GetCollection<Certificate>("certificates");
        _certificates.EnsureIndex(x => x.Domains);

        _settings = _database.GetCollection<BsonDocument>("settings");
    }

    // Routes
    public IEnumerable<ProxyRoute> GetAllRoutes() => _routes.FindAll();

    public IEnumerable<ProxyRoute> GetEnabledRoutes() => _routes.Find(r => r.Enabled);

    public ProxyRoute? GetRoute(Guid id) => _routes.FindById(id);

    public ProxyRoute? GetRouteByName(string name) => _routes.FindOne(r => r.Name == name);

    public void UpsertRoute(ProxyRoute route)
    {
        route.UpdatedAt = DateTime.UtcNow;
        _routes.Upsert(route);
    }

    public bool DeleteRoute(Guid id) => _routes.Delete(id);

    // Certificates
    public IEnumerable<Certificate> GetAllCertificates() => _certificates.FindAll();

    public Certificate? GetCertificate(Guid id) => _certificates.FindById(id);

    public Certificate? GetCertificateByDomain(string domain) =>
        _certificates.FindOne(c => c.Domains.Contains(domain));

    public IEnumerable<Certificate> GetCertificatesNeedingRenewal(int daysBeforeExpiry)
    {
        var threshold = DateTime.UtcNow.AddDays(daysBeforeExpiry);
        return _certificates.Find(c => c.AutoRenew && c.ExpiresAt != null && c.ExpiresAt < threshold);
    }

    public void UpsertCertificate(Certificate cert) => _certificates.Upsert(cert);

    public bool DeleteCertificate(Guid id) => _certificates.Delete(id);

    // Settings
    public AppSettings GetSettings()
    {
        var doc = _settings.FindById("app");
        if (doc == null)
            return new AppSettings();

        return new AppSettings
        {
            PasswordHash = doc["PasswordHash"]?.AsString,
            AcmeEmail = doc["AcmeEmail"]?.AsString,
            UseAcmeStaging = doc["UseAcmeStaging"]?.AsBoolean ?? true,
            AcmeAccountKey = doc["AcmeAccountKey"]?.AsBinary,
            HttpPort = doc["HttpPort"]?.AsInt32 ?? 80,
            HttpsPort = doc["HttpsPort"]?.AsInt32 ?? 443,
            ManagementPort = doc["ManagementPort"]?.AsInt32 ?? 8080,
            DataPath = doc["DataPath"]?.AsString ?? "data",
            CertificateRenewalDays = doc["CertificateRenewalDays"]?.AsInt32 ?? 30,
            EnableAccessLog = doc["EnableAccessLog"]?.AsBoolean ?? true
        };
    }

    public void SaveSettings(AppSettings settings)
    {
        var doc = new BsonDocument
        {
            ["_id"] = "app",
            ["PasswordHash"] = settings.PasswordHash,
            ["AcmeEmail"] = settings.AcmeEmail,
            ["UseAcmeStaging"] = settings.UseAcmeStaging,
            ["AcmeAccountKey"] = settings.AcmeAccountKey,
            ["HttpPort"] = settings.HttpPort,
            ["HttpsPort"] = settings.HttpsPort,
            ["ManagementPort"] = settings.ManagementPort,
            ["DataPath"] = settings.DataPath,
            ["CertificateRenewalDays"] = settings.CertificateRenewalDays,
            ["EnableAccessLog"] = settings.EnableAccessLog
        };
        _settings.Upsert(doc);
    }

    public void Dispose()
    {
        _database.Dispose();
    }
}
