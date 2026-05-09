namespace BarbershopCrm.Domain.Entities;

public class WorkSchedule
{
    public int WorkScheduleId { get; set; }
    public int MasterId { get; set; }
    public int BranchId { get; set; }
    public DateOnly WorkDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string ScheduleType { get; set; } = Domain.Enums.ScheduleType.Work;

    public Master Master { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
