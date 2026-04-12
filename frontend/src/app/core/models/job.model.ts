export interface Job {
  id: number;
  title: string;
  department?: string;
  seniorityLevel?: string;
  locationRaw?: string;
  isRemote: boolean;
  isHybrid?: boolean;
  isRemoteInDescription?: boolean;
  applyUrl?: string;
  firstSeenAt: string;
  authenticityScore?: number;
  authenticityLabel?: string;
  source?: { name: string };
  company?: { id: number; canonicalName: string; logoUrl?: string; industry?: string };
  companyId?: number;
  companyName?: string;
}

export interface JobDetail extends Job {
  description?: string;
  descriptionHtml?: string;
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
  skill?: string;
  companyId?: number;
  isRemote?: boolean;
  isUs?: boolean;
  seniority?: string;
  department?: string;
  industries?: string[];
  page?: number;
  pageSize?: number;
}
