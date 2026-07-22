using GitTool.App.Services;
using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

VerifyPackagedDataMigration();
VerifyNotificationManifestDetection();
await VerifyElevatedSessionAsync();
await VerifyUnsupportedSessionAsync();
await VerifyRegistrationFailureAsync();
await VerifyDeliveryPolicyAsync();
await VerifySystemSettingsAsync();
await VerifySendFailureAsync();
Console.WriteLine("[OK] Package-data migration and notification capability, registration, delivery, and background-policy checks passed.");
return;

static void VerifyPackagedDataMigration()
{
    var testRoot = Path.Combine(Path.GetTempPath(), $"GitTool-AppData-{Guid.NewGuid():N}");
    var legacyRoot = Path.Combine(testRoot, "Legacy");
    var packageRoot = Path.Combine(testRoot, "Package");

    try
    {
        Directory.CreateDirectory(Path.Combine(legacyRoot, "Logs"));
        File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "legacy-settings");
        File.WriteAllText(Path.Combine(legacyRoot, "Logs", "GitTool-20260723.log"), "legacy-log");

        AppDataStorage.MigrateLegacyData(legacyRoot, packageRoot);

        AssertTrue(!Directory.Exists(legacyRoot), "legacy data root was not removed after migration");
        AssertTrue(
            File.Exists(Path.Combine(packageRoot, "settings.json")),
            "settings were not moved into package storage");
        AssertTrue(
            File.Exists(Path.Combine(packageRoot, "Logs", "GitTool-20260723.log")),
            "logs were not moved into package storage");

        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "new-legacy-settings");

        AppDataStorage.MigrateLegacyData(legacyRoot, packageRoot);

        AssertTrue(!Directory.Exists(legacyRoot), "merged legacy data root was not removed");
        AssertEqual(
            2,
            Directory.EnumerateFiles(packageRoot, "settings*.json").Count(),
            "colliding settings files preserved during migration");
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}

static void VerifyNotificationManifestDetection()
{
    var testRoot = Path.Combine(Path.GetTempPath(), $"GitTool-Manifest-{Guid.NewGuid():N}");
    var manifestPath = Path.Combine(testRoot, "AppxManifest.xml");

    try
    {
        Directory.CreateDirectory(testRoot);
        File.WriteAllText(
            manifestPath,
            """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
                     xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
              <Applications>
                <Application>
                  <Extensions>
                    <desktop:Extension Category="windows.toastNotificationActivation" />
                    <com:Extension Category="windows.comServer" />
                  </Extensions>
                </Application>
              </Applications>
            </Package>
            """);
        AssertTrue(
            PackageIdentity.ManifestDeclaresNotificationComActivator(manifestPath),
            "notification COM activator was not detected");

        File.WriteAllText(
            manifestPath,
            """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Applications><Application /></Applications>
            </Package>
            """);
        AssertTrue(
            !PackageIdentity.ManifestDeclaresNotificationComActivator(manifestPath),
            "manifest without notification extensions was treated as COM activated");
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}

static void AssertTrue(bool condition, string subject)
{
    if (!condition)
    {
        throw new InvalidOperationException($"[FAIL] {subject}.");
    }
}

static async Task VerifyElevatedSessionAsync()
{
    var platform = new FakeNotificationPlatform();
    var service = CreateService(platform, isElevated: true);

    await service.InitializeAsync(_ => { });

    AssertEqual(NotificationCapabilityStatus.Elevated, service.GetCapability().Status, "elevated capability");
    AssertEqual(0, platform.SupportChecks, "support checks for an elevated process");
    AssertEqual(0, platform.RegisterCalls, "registration calls for an elevated process");
}

static async Task VerifyUnsupportedSessionAsync()
{
    var platform = new FakeNotificationPlatform { Supported = false };
    var service = CreateService(platform);

    await service.InitializeAsync(_ => { });
    var result = await service.ShowTestAsync("Test", "Unsupported");

    AssertEqual(NotificationCapabilityStatus.Unsupported, service.GetCapability().Status, "unsupported capability");
    AssertEqual(NotificationDeliveryStatus.Unsupported, result.Status, "unsupported delivery");
    AssertEqual(0, platform.RegisterCalls, "registration calls when unsupported");
    AssertEqual(0, platform.ShowCalls, "send calls when unsupported");
}

static async Task VerifyRegistrationFailureAsync()
{
    var platform = new FakeNotificationPlatform
    {
        RegisterException = new InvalidOperationException("registration failed")
    };
    var logger = new TestLogger();
    var service = CreateService(platform, logger: logger);

    await service.InitializeAsync(_ => { });
    var result = await service.ShowTestAsync("Test", "Registration failure");

    AssertEqual(
        NotificationCapabilityStatus.RegistrationFailed,
        service.GetCapability().Status,
        "registration-failure capability");
    AssertEqual(
        NotificationDeliveryStatus.RegistrationFailed,
        result.Status,
        "registration-failure delivery");
    AssertEqual(1, logger.Errors.Count, "logged registration failures");
}

static async Task VerifyDeliveryPolicyAsync()
{
    var settings = AppSettings.CreateDefault();
    settings.NotificationsEnabled = false;
    var platform = new FakeNotificationPlatform();
    var invokedArgument = string.Empty;
    var service = CreateService(platform, settings);

    await service.InitializeAsync(argument => invokedArgument = argument);
    AssertEqual(1, platform.RegisterCalls, "registration calls");

    platform.Invoke("action=open");
    AssertEqual("action=open", invokedArgument, "notification invocation argument");

    service.SetForegroundState(true);
    var test = await service.ShowTestAsync("GitTool notification test", "Explicit test");
    AssertEqual(NotificationDeliveryStatus.Sent, test.Status, "foreground test delivery");
    AssertEqual(1, platform.ShowCalls, "foreground test sends");

    var foreground = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Success("Clone complete."));
    AssertEqual(
        NotificationDeliveryStatus.SuppressedWhileForeground,
        foreground.Status,
        "foreground operation delivery");

    service.SetForegroundState(false);
    var disabled = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Success("Clone complete."));
    AssertEqual(NotificationDeliveryStatus.DisabledByGitTool, disabled.Status, "app-disabled delivery");

    settings.NotificationsEnabled = true;
    var success = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Success("Clone complete."));
    AssertEqual(NotificationDeliveryStatus.Sent, success.Status, "background success delivery");
    AssertEqual("Clone repository completed", platform.LastTitle, "success notification title");

    var cancellation = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Cancelled("Clone cancelled."));
    AssertEqual(NotificationDeliveryStatus.Sent, cancellation.Status, "background cancellation delivery");
    AssertEqual("Clone repository cancelled", platform.LastTitle, "cancellation notification title");

    var cleanupWarning = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Cancelled(
            "Cleanup failed.",
            cancellation: new OperationCancellationMetadata(true, false, "partial")));
    AssertEqual(NotificationDeliveryStatus.Sent, cleanupWarning.Status, "cleanup-warning delivery");
    AssertEqual("GitTool needs attention", platform.LastTitle, "cleanup-warning notification title");

    service.SuppressForShutdown();
    var shutdown = await service.ShowOperationCompletionAsync(
        "Clone repository",
        OperationResult.Cancelled());
    AssertEqual(
        NotificationDeliveryStatus.SuppressedForShutdown,
        shutdown.Status,
        "shutdown notification delivery");

    service.Shutdown();
    service.Shutdown();
    AssertEqual(1, platform.UnregisterCalls, "idempotent notification cleanup");
}

static async Task VerifySystemSettingsAsync()
{
    var cases = new[]
    {
        (NotificationSystemSetting.DisabledForApplication, NotificationDeliveryStatus.DisabledForApplication),
        (NotificationSystemSetting.DisabledForUser, NotificationDeliveryStatus.DisabledForUser),
        (NotificationSystemSetting.DisabledByGroupPolicy, NotificationDeliveryStatus.DisabledByGroupPolicy),
        (NotificationSystemSetting.DisabledByManifest, NotificationDeliveryStatus.DisabledByManifest),
        (NotificationSystemSetting.Unsupported, NotificationDeliveryStatus.Unsupported)
    };

    foreach (var (setting, expected) in cases)
    {
        var platform = new FakeNotificationPlatform { Setting = setting };
        var service = CreateService(platform);
        await service.InitializeAsync(_ => { });

        var result = await service.ShowTestAsync("Test", setting.ToString());
        AssertEqual(expected, result.Status, $"delivery for {setting}");
        AssertEqual(0, platform.ShowCalls, $"send calls for {setting}");
        service.Shutdown();
    }
}

static async Task VerifySendFailureAsync()
{
    var platform = new FakeNotificationPlatform
    {
        ShowException = new InvalidOperationException("send failed")
    };
    var logger = new TestLogger();
    var service = CreateService(platform, logger: logger);
    await service.InitializeAsync(_ => { });

    var result = await service.ShowTestAsync("Test", "Failure");

    AssertEqual(NotificationDeliveryStatus.SendFailed, result.Status, "send-failure delivery");
    AssertEqual(1, logger.Errors.Count, "logged send failures");
    service.Shutdown();
}

static AppNotificationService CreateService(
    FakeNotificationPlatform platform,
    AppSettings? settings = null,
    bool isElevated = false,
    TestLogger? logger = null)
{
    settings ??= AppSettings.CreateDefault();
    logger ??= new TestLogger();
    return new AppNotificationService(
        () => settings,
        logger,
        platform,
        () => isElevated);
}

static void AssertEqual<T>(T expected, T actual, string subject)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"[FAIL] Expected {subject} to be '{expected}', but got '{actual}'.");
    }
}

file sealed class FakeNotificationPlatform : IAppNotificationPlatform
{
    private Action<string>? _notificationInvoked;

    public bool Supported { get; init; } = true;

    public NotificationSystemSetting Setting { get; set; } = NotificationSystemSetting.Enabled;

    public Exception? RegisterException { get; init; }

    public Exception? ShowException { get; init; }

    public int SupportChecks { get; private set; }

    public int RegisterCalls { get; private set; }

    public int ShowCalls { get; private set; }

    public int UnregisterCalls { get; private set; }

    public string LastTitle { get; private set; } = string.Empty;

    public bool IsSupported()
    {
        SupportChecks++;
        return Supported;
    }

    public NotificationSystemSetting GetSetting() => Setting;

    public void Register(Action<string> notificationInvoked)
    {
        RegisterCalls++;
        if (RegisterException is not null)
        {
            throw RegisterException;
        }

        _notificationInvoked = notificationInvoked;
    }

    public void Show(string title, string message)
    {
        ShowCalls++;
        if (ShowException is not null)
        {
            throw ShowException;
        }

        LastTitle = title;
    }

    public void Unregister()
    {
        UnregisterCalls++;
        _notificationInvoked = null;
    }

    public void Invoke(string argument) => _notificationInvoked?.Invoke(argument);
}

file sealed class TestLogger : IAppLogger
{
    public List<string> Errors { get; } = [];

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public Task ErrorAsync(string message, Exception? exception = null)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
