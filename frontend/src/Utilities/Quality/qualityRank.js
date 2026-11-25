// A missing recording is below a file with an unranked quality. The server
// normalizes each saved profile to 0..1 and gives grouped qualities equal rank.
export function bookFileQualityRank(item) {
  return item.qualityRank ?? -1;
}

export function recordingQualityRank(item) {
  return item.statistics?.fileQualityRank ?? -2;
}

export function formatRecordingQualityRank(rank) {
  if (rank == null) {
    return 'No files';
  }

  return rank < 0 ? 'Unranked' : `${Math.round(rank * 100)}%`;
}
