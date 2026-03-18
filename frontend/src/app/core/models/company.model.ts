export interface Company {
  id: number;
  canonicalName: string;
  industry?: string;
  employeeCountRange?: string;
  headquartersCity?: string;
  headquartersCountry?: string;
  logoUrl?: string;
  activeJobCount: number;
}
