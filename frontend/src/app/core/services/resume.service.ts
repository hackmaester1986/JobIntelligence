import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ResumeMatchResult, ResumeResult } from '../models/resume.model';

@Injectable({ providedIn: 'root' })
export class ResumeService {
  private http = inject(HttpClient);
  private base = environment.apiUrl;

  uploadResume(file: File): Observable<ResumeResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ResumeResult>(`${this.base}/resumes/upload`, form);
  }

  getMatches(resumeId: number, limit = 20, isUs?: boolean): Observable<ResumeMatchResult> {
    let params = new HttpParams().set('limit', limit);
    if (isUs !== undefined) params = params.set('isUs', isUs);
    return this.http.get<ResumeMatchResult>(`${this.base}/resumes/${resumeId}/matches`, { params });
  }
}
