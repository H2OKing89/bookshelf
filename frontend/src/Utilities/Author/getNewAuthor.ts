import type { StateRecord } from 'Store/StateTypes';

export default function getNewAuthor(author: StateRecord, payload: StateRecord): StateRecord {
  const { rootFolderPath, monitor, monitorNewItems, qualityProfileId, metadataProfileId, tags, searchForMissingBooks = false } = payload;
  return {
    ...author,
    addOptions: { monitor, searchForMissingBooks },
    monitored: true,
    monitorNewItems,
    qualityProfileId,
    metadataProfileId,
    rootFolderPath,
    tags
  };
}
