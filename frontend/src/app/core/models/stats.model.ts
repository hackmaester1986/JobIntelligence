export interface TopCompany {
  id: number;
  name: string;
  logoUrl?: string;
  jobCount: number;
}

export interface SeniorityBucket {
  label: string;
  count: number;
}

export interface DepartmentBucket {
  department: string;
  count: number;
}

export interface DashboardStats {
  totalActiveJobs: number;
  totalCompanies: number;
  remoteJobs: number;
  hybridJobs: number;
  onsiteJobs: number;
  activeToday: number;
  topCompanies: TopCompany[];
  jobsBySeniority: SeniorityBucket[];
  topDepartments: DepartmentBucket[];
  snapshotAt?: string;
}
