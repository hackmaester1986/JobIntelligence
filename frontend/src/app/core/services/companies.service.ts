import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Company, SnapshotPoint } from '../models/company.model';
import { Job } from '../models/job.model';
import { ApiService } from './api.service';
import { PagedResult } from './jobs.service';

@Injectable({ providedIn: 'root' })
export class CompaniesService {
  private api = inject(ApiService);

  getIndustries(): Observable<string[]> {
    return this.api.get<string[]>('/companies/industries');
  }

  getCompanies(q?: string, page = 1, pageSize = 50, industries?: string[], isUs?: boolean): Observable<PagedResult<Company>> {
    return this.api.get<PagedResult<Company>>('/companies', { q, page, pageSize, industries, isUs });
  }

  getCompany(id: number, isUs?: boolean): Observable<Company> {
    return this.api.get<Company>(`/companies/${id}`, isUs != null ? { isUs } : undefined);
  }

  getCompanyJobs(id: number, isUs?: boolean): Observable<Job[]> {
    return this.api.get<Job[]>(`/companies/${id}/jobs`, isUs != null ? { isUs } : undefined);
  }

  getSnapshots(id: number, range: string): Observable<SnapshotPoint[]> {
    return this.api.get<SnapshotPoint[]>(`/companies/${id}/snapshots`, { range });
  }
}
