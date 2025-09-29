using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GourmetClientApp.Model;
using GourmetClientApp.Notifications;
using GourmetClientApp.Settings;
using GourmetClientApp.Utils;

namespace GourmetClientApp.Network;

public class BillingCacheService
{
    private readonly GourmetSettingsService _settingsService;
    private readonly GourmetWebClient _gourmetWebClient;
    private readonly VentopayWebClient _ventopayWebClient;
    private readonly NotificationService _notificationService;

    public BillingCacheService()
    {
        _settingsService = InstanceProvider.SettingsService;
        _gourmetWebClient = InstanceProvider.GourmetWebClient;
        _ventopayWebClient = InstanceProvider.VentopayWebClient;
        _notificationService = InstanceProvider.NotificationService;
    }

    public async Task<IReadOnlyCollection<BillingPosition>> GetBillingPositions(int month, int year, IProgress<int> progress)
    {
        var gourmetProgress = new Progress<int>();
        var ventopayProgress = new Progress<int>();
        using var totalProgressWrapper = new TotalProgressWrapper(progress, [gourmetProgress, ventopayProgress]);

        Task<IReadOnlyList<BillingPosition>> gourmetTask = GetBillingPositions(
            "Gourmet",
            userSettings => !string.IsNullOrEmpty(userSettings.GourmetLoginUsername),
            userSettings => _gourmetWebClient.Login(userSettings.GourmetLoginUsername, userSettings.GourmetLoginPassword),
            () => _gourmetWebClient.GetBillingPositions(month, year, gourmetProgress),
            gourmetProgress);

        Task<IReadOnlyList<BillingPosition>> ventopayTask = GetBillingPositions(
            "Ventopay",
            userSettings => !string.IsNullOrEmpty(userSettings.VentopayUsername),
            userSettings => _ventopayWebClient.Login(userSettings.VentopayUsername, userSettings.VentopayPassword),
            () =>
            {
                var fromDate = new DateTime(year, month, 1);
                var toDate = fromDate.AddMonths(1).AddDays(-1);
                return _ventopayWebClient.GetBillingPositions(fromDate, toDate, ventopayProgress);
            },
            ventopayProgress);

        IReadOnlyList<BillingPosition> gourmetResult = await gourmetTask.ConfigureAwait(false);
        IReadOnlyList<BillingPosition> ventopayResult = await ventopayTask.ConfigureAwait(false);

        var billingPositions = new List<BillingPosition>();
        billingPositions.AddRange(gourmetResult);
        billingPositions.AddRange(ventopayResult);

        return billingPositions;
    }

    private async Task<IReadOnlyList<BillingPosition>> GetBillingPositions(
        string sourceName,
        Func<UserSettings, bool> userSettingsValidationFunc,
        Func<UserSettings, Task<LoginHandle>> loginFunc,
        Func<Task<IReadOnlyList<BillingPosition>>> getBillingPositionsFunc,
        IProgress<int> subProgress)
    {
        UserSettings userSettings = _settingsService.GetCurrentUserSettings();
        if (!userSettingsValidationFunc.Invoke(userSettings))
        {
            _notificationService.Send(
                new Notification(
                    NotificationType.Warning,
                    $"Zugangsdaten für {sourceName} sind nicht konfiguriert. Abrechnungsdaten sind unvollständig"));
            subProgress.Report(100);
            return [];
        }

        try
        {
            await using LoginHandle loginHandle = await loginFunc.Invoke(userSettings);
            if (!loginHandle.LoginSuccessful)
            {
                _notificationService.Send(
                    new Notification(
                        NotificationType.Error,
                        $"Abrechnungsdaten von {sourceName} konnten nicht geladen werden. Ursache: Login fehlgeschlagen"));
                return [];
            }

            return await getBillingPositionsFunc.Invoke();
        }
        catch (Exception exception) when (exception is GourmetRequestException || exception is GourmetParseException)
        {
            _notificationService.Send(new ExceptionNotification($"Abrechnungsdaten von {sourceName} konnten nicht geladen werden", exception));
            return [];
        }
        finally
        {
            subProgress.Report(100);
        }
    }

    private sealed class TotalProgressWrapper : IDisposable
    {
        private readonly IProgress<int> _targetProgress;
        private readonly IList<Progress<int>> _sourceProgresses;
        private readonly int[] _progressValues;

        public TotalProgressWrapper(IProgress<int> targetProgress, IList<Progress<int>> sourceProgresses)
        {
            _targetProgress = targetProgress;
            _sourceProgresses = sourceProgresses;
            _progressValues = new int[sourceProgresses.Count];

            for (int i = 0; i < _sourceProgresses.Count; i++)
            {
                _progressValues[i] = 0;
                _sourceProgresses[i].ProgressChanged += OnProgressChanged;
            }
        }

        public void Dispose()
        {
            foreach (Progress<int> sourceProgress in _sourceProgresses)
            {
                sourceProgress.ProgressChanged -= OnProgressChanged;
            }
        }

        private void OnProgressChanged(object? sender, int value)
        {
            Debug.Assert(sender is Progress<int>);
            Debug.Assert(value is >= 0 and <= 100);

            int index = _sourceProgresses.IndexOf((Progress<int>)sender);
            _progressValues[index] = value;

            int totalProgress = _progressValues.Sum() / _progressValues.Length;
            _targetProgress.Report(totalProgress);
        }
    }
}