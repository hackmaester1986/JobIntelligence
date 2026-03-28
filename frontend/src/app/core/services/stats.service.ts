import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { DashboardStats } from '../models/stats.model';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class StatsService {
  private api = inject(ApiService);

  getStats(isUs?: boolean): Observable<DashboardStats> {
    return this.api.get<DashboardStats>('/stats', isUs != null ? { isUs } : undefined);
  }
}
