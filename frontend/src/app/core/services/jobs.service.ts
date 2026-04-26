import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Job, JobDetail, JobFilters } from '../models/job.model';
import { ApiService } from './api.service';

export interface PagedResult<T> {
  total: number;
  page: number;
  pageSize: number;
  proximityMode?: boolean;
  data: T[];
}

@Injectable({ providedIn: 'root' })
export class JobsService {
  private api = inject(ApiService);

  getJobs(filters: JobFilters = {}): Observable<PagedResult<Job>> {
    return this.api.get<PagedResult<Job>>('/jobs', filters as Record<string, string | number | boolean | undefined>);
  }

  getJob(id: number): Observable<JobDetail> {
    return this.api.get<JobDetail>(`/jobs/${id}`);
  }
}
