using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>One account in the settings' "Account data" list</summary>
internal sealed class StoredAccountRowViewModel(StoredAccount account)
{
    public StoredAccount Account { get; } = account;
    public string Title => Account.Title;

    public string StatusText =>
        Account.IsCurrent ? Strings.AccountDataCurrent
        : Account.SaveFolderExists ? Strings.AccountDataPresent
        : Account.GoneSinceUtc is { } since
            ? string.Format(Strings.AccountDataGoneSinceFormat, since.ToLocalTime().ToString("d", AppLocalisationService.Culture))
            : Strings.AccountDataGone;

    public string SizeText => ByteSizeFormatter.Format(Account.Bytes);
    public bool IsGone => !Account.SaveFolderExists;

    /// <summary>In the interface language of when the row was made (the rows are made again after a language change)</summary>
    public string ForgetButtonText => Strings.AccountDataForgetButton;

    /// <summary>The open account cannot be forgotten: its cache is in use</summary>
    public bool CanForget => !Account.IsCurrent;
}
