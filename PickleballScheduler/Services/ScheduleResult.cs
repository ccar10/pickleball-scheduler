using PickleballScheduler.Models;

namespace PickleballScheduler.Services;

public record ScheduleResult(
    List<Round> Rounds,
    int Hr1Violations,
    int Hr2Violations,
    string? RepeatSuggestion);
