namespace HRMS.Domain.Enums;

public enum SeparationExitInterviewTemplateVersionStatus { Draft, Published, Retired }
public enum SeparationExitInterviewQuestionType { SingleChoice, MultiChoice, Rating, YesNo, ShortText, LongText, Number, Date }
public enum SeparationExitInterviewStatus { NotStarted, EmployeeInProgress, EmployeeSubmitted, HrInProgress, Completed, CompletedWithoutEmployeeResponse, Reopened, Cancelled }
public enum SeparationExitInterviewRehireRecommendation { Recommended, NotRecommended, Conditional, NotAssessed }
public enum SeparationExitInterviewEventType { InterviewAssigned, EmployeeDraftSaved, EmployeeSubmitted, HrInterviewStarted, HrNoteAdded, InterviewCompleted, InterviewCompletedWithoutEmployee, InterviewReopened, InterviewCancelled }
