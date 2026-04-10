import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface DistributionBin {
  bucket: string;
  count: number;
}

export interface BucketCompany {
  id: number;
  canonicalName: string;
  logoUrl?: string;
  industry?: string;
  activeJobCount: number;
  employeeCountRange?: string;
  headquartersCity?: string;
  headquartersCountry?: string;
}

export interface BucketCompaniesResult {
  bucket: string;
  companies: BucketCompany[];
}

@Injectable({ providedIn: 'root' })
export class AnalysisService {
  private api = inject(ApiService);

  getSizes(): Observable<string[]> {
    return this.api.get<string[]>('/analysis/sizes');
  }

  getDistribution(metric: string, sizes: string[], isUs?: boolean): Observable<DistributionBin[]> {
    const params: Record<string, string | boolean | string[] | undefined> = { metric };
    if (sizes.length > 0) params['sizes'] = sizes;
    if (isUs != null) params['isUs'] = isUs;
    return this.api.get<DistributionBin[]>('/analysis/distribution', params);
  }

  getBucketCompanies(metric: string, bucket: string, sizes: string[], isUs?: boolean): Observable<BucketCompaniesResult> {
    const params: Record<string, string | boolean | string[] | undefined> = { metric, bucket };
    if (sizes.length > 0) params['sizes'] = sizes;
    if (isUs != null) params['isUs'] = isUs;
    return this.api.get<BucketCompaniesResult>('/analysis/bucket-companies', params);
  }
}
