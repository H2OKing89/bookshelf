// A missing recording is below a file with an unranked quality. The server
// normalizes each saved profile to 0..1 and gives grouped qualities equal rank.
export function bookFileQualityRank(item: { qualityRank?: number | null }) {
  return item.qualityRank ?? -1;
}

export function recordingQualityRank(item: { statistics?: { fileQualityRank?: number | null } | null }) {
  return item.statistics?.fileQualityRank ?? -2;
}

export function formatRecordingQualityRank(rank: number | null | undefined) {
  if (rank == null) {
    return 'No files';
  }

  return rank < 0 ? 'Unranked' : `${Math.round(rank * 100)}%`;
}
