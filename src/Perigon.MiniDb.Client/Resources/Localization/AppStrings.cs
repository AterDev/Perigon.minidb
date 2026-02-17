namespace Perigon.MiniDb.Client.Resources.Localization;

/// <summary>
/// Marker type for IStringLocalizer resource lookup.
/// </summary>
public sealed class AppStrings
{
	public static class Keys
	{
		public const string AppTitle = "App.Title";
		public const string WindowConnectionManagerTitle = "Window.ConnectionManager.Title";

		public const string MenuConnection = "Menu.Connection";
		public const string MenuManageConnections = "Menu.ManageConnections";
		public const string MenuAppearance = "Menu.Appearance";
		public const string MenuLightTheme = "Menu.Theme.Light";
		public const string MenuDarkTheme = "Menu.Theme.Dark";
		public const string MenuSystemTheme = "Menu.Theme.System";
		public const string MenuToggleGlass = "Menu.ToggleGlass";
		public const string MenuLanguage = "Menu.Language";
		public const string MenuHelp = "Menu.Help";
		public const string MenuOpenRepo = "Menu.OpenRepo";
		public const string MenuOpenIssues = "Menu.OpenIssues";

		public const string SectionTables = "Section.Tables";
		public const string SectionConnectionConfig = "Section.ConnectionConfig";

		public const string LabelSearchTableWatermark = "Label.SearchTableWatermark";
		public const string LabelSearchConnectionWatermark = "Label.SearchConnectionWatermark";
		public const string LabelConnectionNameWatermark = "Label.ConnectionNameWatermark";
		public const string LabelDbPathWatermark = "Label.DbPathWatermark";
		public const string LabelFilterWatermark = "Label.FilterWatermark";
		public const string LabelNoDataTitle = "Label.NoDataTitle";
		public const string LabelNoConnection = "Label.NoConnection";
		public const string LabelNotSelectedTable = "Label.NotSelectedTable";
		public const string LabelNoSelectedConnection = "Label.NoSelectedConnection";
		public const string LabelAddOrSelectConnectionFirst = "Label.AddOrSelectConnectionFirst";
		public const string LabelSelectTableToView = "Label.SelectTableToView";
		public const string LabelNoDataForFilter = "Label.NoDataForFilter";

		public const string StatusReady = "Status.Ready";
		public const string StatusConnected = "Status.Connected";
		public const string StatusDisconnected = "Status.Disconnected";
		public const string StatusLanguageChanged = "Status.LanguageChanged";

		public const string ButtonConnect = "Button.Connect";
		public const string ButtonDisconnect = "Button.Disconnect";
		public const string ButtonCreateSample = "Button.CreateSample";
		public const string ButtonResetView = "Button.ResetView";
		public const string ButtonApply = "Button.Apply";
		public const string ButtonClear = "Button.Clear";
		public const string ButtonFirstPage = "Button.FirstPage";
		public const string ButtonPrevPage = "Button.PrevPage";
		public const string ButtonNextPage = "Button.NextPage";
		public const string ButtonLastPage = "Button.LastPage";
		public const string ButtonBrowse = "Button.Browse";
		public const string ButtonAdd = "Button.Add";
		public const string ButtonUpdate = "Button.Update";
		public const string ButtonDelete = "Button.Delete";
		public const string ButtonClose = "Button.Close";

		public const string DialogSelectMiniDbFile = "Dialog.SelectMiniDbFile";
		public const string DialogCreateSampleDb = "Dialog.CreateSampleDb";

		public const string MessageSelectConnectionFirst = "Message.SelectConnectionFirst";
		public const string MessageConnectionAdded = "Message.ConnectionAdded";
		public const string MessageConnectionUpdated = "Message.ConnectionUpdated";
		public const string MessageConnectionDeleted = "Message.ConnectionDeleted";
		public const string MessageAddConnectionFailed = "Message.AddConnectionFailed";
		public const string MessageUpdateConnectionFailed = "Message.UpdateConnectionFailed";
		public const string MessageDeleteConnectionFailed = "Message.DeleteConnectionFailed";
		public const string MessageDatabaseFileNotFound = "Message.DatabaseFileNotFound";
		public const string MessageDatabaseFileNotFoundWithPath = "Message.DatabaseFileNotFoundWithPath";
		public const string MessageConnectedTo = "Message.ConnectedTo";
		public const string MessageConnectionFailed = "Message.ConnectionFailed";
		public const string MessageReleaseCacheFailed = "Message.ReleaseCacheFailed";
		public const string MessageDisconnected = "Message.Disconnected";
		public const string MessageReadTableDataFailed = "Message.ReadTableDataFailed";
		public const string MessageLoadTableDataFailed = "Message.LoadTableDataFailed";
		public const string MessageLoadedTableWithCount = "Message.LoadedTableWithCount";
		public const string MessageFilterCompleted = "Message.FilterCompleted";
		public const string MessageFilterCleared = "Message.FilterCleared";
		public const string MessageSampleDbCreated = "Message.SampleDbCreated";
		public const string MessageSampleDbCreateFailed = "Message.SampleDbCreateFailed";
		public const string MessageUnableToGetTargetPath = "Message.UnableToGetTargetPath";
		public const string MessageRepositoryOpened = "Message.RepositoryOpened";
		public const string MessageOpenRepositoryFailed = "Message.OpenRepositoryFailed";
		public const string MessageIssuesOpened = "Message.IssuesOpened";
		public const string MessageOpenIssuesFailed = "Message.OpenIssuesFailed";

		public const string FormatTableTitle = "Format.TableTitle";
		public const string FormatPageSummary = "Format.PageSummary";
		public const string FormatFilterSummary = "Format.FilterSummary";
		public const string FormatTableCount = "Format.TableCount";
		public const string FormatFilteredCount = "Format.FilteredCount";
		public const string FormatRawCount = "Format.RawCount";
	}
}
