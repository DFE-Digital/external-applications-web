namespace DfE.ExternalApplications.Domain.Models;

public enum TaskStatus
{
    /// <summary>
    /// Task has not been started - no fields have been completed
    /// </summary>
    NotStarted = 0,
    
    /// <summary>
    /// Task is in progress - some but not all required fields have been completed
    /// </summary>
    InProgress = 1,
    
    /// <summary>
    /// Task is completed - all required fields have been completed
    /// </summary>
    Completed = 2,
    
    /// <summary>
    /// Task cannot be completed - validation errors or missing dependencies
    /// </summary>
    CannotStartYet = 3
} 