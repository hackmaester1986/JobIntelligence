import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface DistributionBin {
  bucket: string;
  count: number;
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
}
