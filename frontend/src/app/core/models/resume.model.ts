export interface ResumeResult {
  id: number;
  name?: string;
  email?: string;
  location?: string;
  yearsOfExperience?: number;
  educationLevel?: string;
  educationField?: string;
  skills: string[];
  recentJobTitles: string[];
  industries: string[];
  createdAt: string;
}

export interface ResumeJobMatch {
  jobPostingId: number;
  title: string;
  companyName?: string;
  companyLogoUrl?: string;
  industry?: string;
  seniorityLevel?: string;
  locationRaw?: string;
  isRemote: boolean;
  isHybrid: boolean;
  applyUrl?: string;
  similarity: number;
}

export interface ResumeMatchResult {
  resumeId: number;
  matches: ResumeJobMatch[];
}
