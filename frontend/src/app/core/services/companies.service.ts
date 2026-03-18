import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Company } from '../models/company.model';
import { Job } from '../models/job.model';
import { ApiService } from './api.service';
import { PagedResult } from './jobs.service';

@Injectable({ providedIn: 'root' })
export class CompaniesService {
  private api = inject(ApiService);

  getCompanies(q?: string, page = 1, pageSize = 50): Observable<PagedResult<Company>> {
    return this.api.get<PagedResult<Company>>('/companies', { q, page, pageSize });
  }

  getCompany(id: number): Observable<Company> {
    return this.api.get<Company>(`/companies/${id}`);
  }

  getCompanyJobs(id: number): Observable<Job[]> {
    return this.api.get<Job[]>(`/companies/${id}/jobs`);
  }
}
