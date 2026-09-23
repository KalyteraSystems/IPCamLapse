using System.ComponentModel.DataAnnotations;
using System.Globalization;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPCamLapse.Pages;

public sealed class SettingsModel : PageModel
{
    private readonly IApplicationSettingsService _settings;
    private readonly IStorageService _storage;

    public SettingsModel(IApplicationSettingsService settings, IStorageService storage)
    {
        _settings = settings;
        _storage = storage;
    }

    [BindProperty, Range(0.1, 10240)]
    public double MaxStorageGb { get; set; }

    [BindProperty, Range(0.05, 1024)]
    public double MinimumFreeGb { get; set; }

    [BindProperty, Range(0, 3650)]
    public int RetentionDays { get; set; }

    [BindProperty, Range(1, 102400)]
    public int EstimatedFrameKb { get; set; }

    public StorageStatus Storage { get; private set; } = new(0, 1, 0, 0, false, null);

    public RetentionPreview RetentionPreview { get; private set; } = RetentionPreview.Disabled;

    public async Task OnGetAsync()
    {
        LoadValues();
        Storage = await _storage.GetStatusAsync();
        RetentionPreview = await _storage.PreviewRetentionAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Storage = await _storage.GetStatusAsync();
            RetentionPreview = await _storage.PreviewRetentionAsync(HttpContext.RequestAborted);
            return Page();
        }
        await _settings.SaveAsync(new ApplicationSettings
        {
            MaxStorageBytes = (long)(MaxStorageGb * 1024 * 1024 * 1024),
            MinimumFreeBytes = (long)(MinimumFreeGb * 1024 * 1024 * 1024),
            RetentionDays = RetentionDays,
            EstimatedFrameBytes = EstimatedFrameKb * 1024L
        }, HttpContext.RequestAborted);
        TempData["Saved"] = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunRetentionAsync()
    {
        var result = await _storage.ApplyRetentionAsync(HttpContext.RequestAborted);
        TempData["RetentionResult"] = result.DeletedSessions;
        // The default TempData serializer rejects long, so this crosses the redirect as an
        // invariant-culture string and is parsed back the same way on the page.
        TempData["RetentionBytes"] = result.DeletedBytes.ToString(CultureInfo.InvariantCulture);
        TempData["RetentionPartial"] = result.EstimateIsPartial;
        if (result.FailedSessions > 0)
            TempData["RetentionFailed"] = string.Join(", ", result.FailedSessionIds);
        return RedirectToPage();
    }

    private void LoadValues()
    {
        var settings = _settings.Current;
        MaxStorageGb = settings.MaxStorageBytes / 1024d / 1024d / 1024d;
        MinimumFreeGb = settings.MinimumFreeBytes / 1024d / 1024d / 1024d;
        RetentionDays = settings.RetentionDays;
        EstimatedFrameKb = (int)(settings.EstimatedFrameBytes / 1024);
    }
}
