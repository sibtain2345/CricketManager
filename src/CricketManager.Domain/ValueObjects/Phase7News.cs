using CricketManager.Domain.Enums;
using CricketManager.Domain.Services;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 7, Slice 7.5: a NewsItem as it sits in the world's persisted archive - the rendered
/// item plus the ids it concerns, so a UI/inbox can filter "news about my club / my players".
/// </summary>
public sealed record NewsArchiveItem(
    DateOnly Date,
    NewsCategory Category,
    int Prominence,
    string Kicker,
    string Headline,
    Guid? SubjectId,
    Guid? SecondarySubjectId);

/// <summary>Phase 7, Slice 7.5: a weekly digest kept in the archive (the WeeklyDigest record flattened for storage).</summary>
public sealed record StoredDigest(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int TotalItems,
    IReadOnlyList<NewsArchiveItem> Lead);

/// <summary>
/// Phase 7, Slice 7.4: a live media storyline a coach has to address - what happened, how hard
/// the questioning is, and whether it is about him personally or his team.
/// </summary>
public sealed record PressStoryline(
    DateOnly Raised,
    string Prompt,
    /// <summary>0-100 - how hostile the room is. A shock sacking rumour or a 6-match losing run is 80+; an ordinary defeat is 40.</summary>
    int Heat,
    bool AboutSelection);
