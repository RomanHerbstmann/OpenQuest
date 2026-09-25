namespace OpenQuest.Core.Domain;

// All enums are stored as snake_case strings (e.g. RemovedAtSource -> "removed_at_source").

public enum ClaimStatus { Active, Submitted, Expired, Cancelled }

public enum SubmissionStatus { Pending, Approved, Rejected }

public enum UserRole { Player, Moderator, Admin }

public enum QuestStatus { Draft, Active, Paused, Full, Closed }

public enum AssetStatus { Active, RemovedAtSource }

/// <summary>Lifecycle of a proposed change to an asset attribute (see ATTRIBUTE_CHANGE in the ERD).</summary>
public enum ChangeStatus { Proposed, Accepted, Exported, Discarded }
