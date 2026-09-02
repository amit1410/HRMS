import type {
  BloodGroup,
  Employee,
  EmployeePersonalDetailsRequest,
  EmployeeSensitiveDetails,
  Gender,
  MaritalStatus,
} from '../../api/types.ts'

/** The list route, and the base every employee URL is built from. */
export const EMPLOYEES_PATH = '/employees'

/** Shown in place of the employee code before a new hire has been saved with a backend-assigned one. */
export const NEW_HIRE_LABEL = 'New Hire'

/** Every value the Personal Details form holds, as strings (what `<input>` and `<select>` deal in). */
export interface PersonalDetailsValues {
  salutation: string
  firstName: string
  middleName: string
  lastName: string
  dateOfBirth: string
  gender: Gender
  bloodGroup: BloodGroup
  maritalStatus: MaritalStatus
  birthCountryId: string
  birthStateId: string
  birthCityId: string
  religion: string
  caste: string
  citizenship: string
  esicApplicable: boolean
  esicNumber: string
  pfNumber: string
  mediclaimNumber: string
  uanNumber: string
  gratuity: boolean
  pension: boolean
  aadhaarNumber: string
  panNumber: string
  dateOfJoining: string
  jobStatus: string
}

/** A blank Personal Details form for a new hire. */
export function emptyPersonalDetailsValues(): PersonalDetailsValues {
  return {
    salutation: '',
    firstName: '',
    middleName: '',
    lastName: '',
    dateOfBirth: '',
    gender: 'Unspecified',
    bloodGroup: 'Unspecified',
    maritalStatus: 'Unspecified',
    birthCountryId: '',
    birthStateId: '',
    birthCityId: '',
    religion: '',
    caste: '',
    citizenship: '',
    esicApplicable: false,
    esicNumber: '',
    pfNumber: '',
    mediclaimNumber: '',
    uanNumber: '',
    gratuity: false,
    pension: false,
    aadhaarNumber: '',
    panNumber: '',
    dateOfJoining: '',
    jobStatus: '',
  }
}

/** An existing employee, unpacked into form values. `null` becomes `''`: an input has no null. */
export function toPersonalDetailsValues(
  employee: Employee,
  sensitive?: Partial<EmployeeSensitiveDetails> | null,
): PersonalDetailsValues {
  return {
    salutation: employee.salutation ?? '',
    firstName: employee.firstName,
    middleName: employee.middleName ?? '',
    lastName: employee.lastName,
    dateOfBirth: employee.dateOfBirth ?? '',
    gender: employee.gender,
    bloodGroup: employee.bloodGroup ?? 'Unspecified',
    maritalStatus: employee.maritalStatus ?? 'Unspecified',
    birthCountryId: employee.birthCountryId ?? '',
    birthStateId: employee.birthStateId ?? '',
    birthCityId: employee.birthCityId ?? '',
    religion: employee.religion ?? '',
    caste: employee.caste ?? '',
    citizenship: employee.citizenship ?? '',
    esicApplicable: employee.esicApplicable,
    esicNumber: sensitive?.esicNumber ?? '',
    pfNumber: sensitive?.pfNumber ?? '',
    mediclaimNumber: sensitive?.mediclaimNumber ?? '',
    uanNumber: sensitive?.uanNumber ?? '',
    gratuity: employee.gratuity,
    pension: employee.pension,
    aadhaarNumber: sensitive?.aadhaarNumber ?? '',
    panNumber: sensitive?.panNumber ?? '',
    dateOfJoining: employee.dateOfJoining,
    jobStatus: employee.jobStatus ?? '',
  }
}

/** The request the Personal Details endpoints accept, trimmed and nullable where optional. */
export function toPersonalDetailsRequest(values: PersonalDetailsValues): EmployeePersonalDetailsRequest {
  return {
    salutation: values.salutation.trim() || null,
    firstName: values.firstName.trim(),
    middleName: values.middleName.trim() || null,
    lastName: values.lastName.trim(),
    dateOfBirth: values.dateOfBirth || null,
    gender: values.gender,
    bloodGroup: values.bloodGroup,
    maritalStatus: values.maritalStatus,
    birthCountryId: values.birthCountryId || null,
    birthStateId: values.birthStateId || null,
    birthCityId: values.birthCityId || null,
    religion: values.religion.trim() || null,
    caste: values.caste.trim() || null,
    citizenship: values.citizenship.trim() || null,
    esicApplicable: values.esicApplicable,
    esicNumber: values.esicNumber.trim() || null,
    pfNumber: values.pfNumber.trim() || null,
    mediclaimNumber: values.mediclaimNumber.trim() || null,
    uanNumber: values.uanNumber.trim() || null,
    gratuity: values.gratuity,
    pension: values.pension,
    aadhaarNumber: values.aadhaarNumber.trim() || null,
    panNumber: values.panNumber.trim() || null,
    dateOfJoining: values.dateOfJoining,
    jobStatus: values.jobStatus.trim() || null,
  }
}
