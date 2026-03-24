export interface Company {
  id: number;
  canonicalName: string;
  industry?: string;
  employeeCountRange?: string;
  headquartersCity?: string;
  headquartersCountry?: string;
  logoUrl?: string;
  activeJobCount: number;
  removedJobCount: number;
  remoteJobCount: number;
  totalJobsEverSeen: number;
  duplicateJobCount: number;
  avgJobLifetimeDays?: number;
  avgRepostCount?: number;
  salaryDisclosureRate?: number;
  statsComputedAt?: string;
}

export interface SnapshotPoint {
  date: string;
  activeJobs: number;
  added: number;
  removed: number;
}
