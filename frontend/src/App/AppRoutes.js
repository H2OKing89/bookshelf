import BlocklistConnector from 'Activity/Blocklist/BlocklistConnector';
import HistoryConnector from 'Activity/History/HistoryConnector';
import QueueConnector from 'Activity/Queue/QueueConnector';
import AuthorDetailsPageConnector from 'Author/Details/AuthorDetailsPageConnector';
import AuthorIndexConnector from 'Author/Index/AuthorIndexConnector';
import BookDetailsPageConnector from 'Book/Details/BookDetailsPageConnector';
import BookIndexConnector from 'Book/Index/BookIndexConnector';
import BookshelfConnector from 'Bookshelf/BookshelfConnector';
import CalendarPageConnector from 'Calendar/CalendarPageConnector';
import NotFound from 'Components/NotFound';
import AddNewItemConnector from 'Search/AddNewItemConnector';
import CustomFormatSettingsConnector from 'Settings/CustomFormats/CustomFormatSettingsConnector';
import DevelopmentSettingsConnector from 'Settings/Development/DevelopmentSettingsConnector';
import DownloadClientSettingsConnector from 'Settings/DownloadClients/DownloadClientSettingsConnector';
import GeneralSettingsConnector from 'Settings/General/GeneralSettingsConnector';
import ImportListSettingsConnector from 'Settings/ImportLists/ImportListSettingsConnector';
import IndexerSettingsConnector from 'Settings/Indexers/IndexerSettingsConnector';
import MediaManagementConnector from 'Settings/MediaManagement/MediaManagementConnector';
import MetadataSettings from 'Settings/Metadata/MetadataSettings';
import NotificationSettings from 'Settings/Notifications/NotificationSettings';
import Profiles from 'Settings/Profiles/Profiles';
import QualityConnector from 'Settings/Quality/QualityConnector';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettingsConnector from 'Settings/UI/UISettingsConnector';
import BackupsConnector from 'System/Backup/BackupsConnector';
import LogsTableConnector from 'System/Events/LogsTableConnector';
import LogFilesConnector from 'System/Logs/Files/LogFilesConnector';
import UpdateLogFilesConnector from 'System/Logs/Updates/UpdateLogFilesConnector';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';
import UnmappedFilesTableConnector from 'UnmappedFiles/UnmappedFilesTableConnector';
import CutoffUnmetConnector from 'Wanted/CutoffUnmet/CutoffUnmetConnector';
import MissingConnector from 'Wanted/Missing/MissingConnector';

export default [
  { index: true, Component: AuthorIndexConnector },
  { path: 'authors', Component: AuthorIndexConnector },
  { path: 'add/search', Component: AddNewItemConnector },
  { path: 'shelf', Component: BookshelfConnector },
  { path: 'books', Component: BookIndexConnector },
  { path: 'unmapped', Component: UnmappedFilesTableConnector },
  { path: 'author/:titleSlug', Component: AuthorDetailsPageConnector },
  { path: 'book/:titleSlug', Component: BookDetailsPageConnector },
  { path: 'calendar', Component: CalendarPageConnector },
  { path: 'activity/history', Component: HistoryConnector },
  { path: 'activity/queue', Component: QueueConnector },
  { path: 'activity/blocklist', Component: BlocklistConnector },
  { path: 'wanted/missing', Component: MissingConnector },
  { path: 'wanted/cutoffunmet', Component: CutoffUnmetConnector },
  { path: 'settings', Component: Settings },
  { path: 'settings/mediamanagement', Component: MediaManagementConnector },
  { path: 'settings/profiles', Component: Profiles },
  { path: 'settings/quality', Component: QualityConnector },
  { path: 'settings/customformats', Component: CustomFormatSettingsConnector },
  { path: 'settings/indexers', Component: IndexerSettingsConnector },
  { path: 'settings/downloadclients', Component: DownloadClientSettingsConnector },
  { path: 'settings/importlists', Component: ImportListSettingsConnector },
  { path: 'settings/connect', Component: NotificationSettings },
  { path: 'settings/metadata', Component: MetadataSettings },
  { path: 'settings/tags', Component: TagSettings },
  { path: 'settings/general', Component: GeneralSettingsConnector },
  { path: 'settings/ui', Component: UISettingsConnector },
  { path: 'settings/development', Component: DevelopmentSettingsConnector },
  { path: 'system/status', Component: Status },
  { path: 'system/tasks', Component: Tasks },
  { path: 'system/backup', Component: BackupsConnector },
  { path: 'system/updates', Component: Updates },
  { path: 'system/events', Component: LogsTableConnector },
  { path: 'system/logs/files', Component: LogFilesConnector },
  { path: 'system/logs/files/update', Component: UpdateLogFilesConnector },
  { path: '*', Component: NotFound }
];
