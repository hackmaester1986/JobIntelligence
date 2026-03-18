export interface Job {
  id: number;
  title: string;
  department?: string;
  seniorityLevel?: string;
  locationRaw?: string;
  isRemote: boolean;
  isHybrid?: boolean;
  firstSeenAt: string;
  authenticityScore?: number;
  authenticityLabel?: string;
  source?: { name: string };
  company?: { id: number; canonicalName: string; logoUrl?: string };
  companyId?: number;
  companyName?: string;
}

export interface JobDetail extends Job {
  description?: string;
  applyUrl?: string;
  salaryMin?: number;
  salaryMax?: number;
  salaryCurrency?: string;
  salaryPeriod?: string;
  salaryDisclosed: boolean;
  team?: string;
  employmentType?: string;
  locationCity?: string;
  locationCountry?: string;
}

export interface JobFilters {
  q?: string;
  companyId?: number;
  isRemote?: boolean;
  seniority?: string;
  department?: string;
  page?: number;
  pageSize?: number;
}
