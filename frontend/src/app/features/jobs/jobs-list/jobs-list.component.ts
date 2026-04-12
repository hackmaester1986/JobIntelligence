import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, skip, switchMap } from 'rxjs/operators';
import { Job, JobFilters } from '../../../core/models/job.model';
import { JobsService, PagedResult } from '../../../core/services/jobs.service';
import { LocationFilterService } from '../../../core/services/location-filter.service';

@Component({
  selector: 'app-jobs-list',
  standalone: true,
  imports: [
    RouterLink, FormsModule, NgFor, NgIf, DecimalPipe,
    MatCardModule, MatInputModule, MatButtonModule,
    MatFormFieldModule, MatProgressSpinnerModule,
    MatPaginatorModule
  ],
  templateUrl: './jobs-list.component.html',
  styleUrl: './jobs-list.component.scss'
})
export class JobsListComponent implements OnInit, OnDestroy {
  private jobsService = inject(JobsService);
  private locationFilter = inject(LocationFilterService);
  private searchSubject = new Subject<void>();
  private loadSubject = new Subject<void>();
  private searchSub!: Subscription;
  private loadSub!: Subscription;
  private locationSub!: Subscription;

  result = signal<PagedResult<Job> | null>(null);
  loading = signal(true);

  filters: JobFilters = { page: 1, pageSize: 20 };
  titleQ = '';
  skillQ = '';

  constructor() {
    this.locationSub = toObservable(this.locationFilter.usOnly).pipe(skip(1)).subscribe(() => {
      this.filters = { ...this.filters, page: 1 };
      this.loadSubject.next();
    });
  }

  ngOnInit(): void {
    const raw = localStorage.getItem('jobsState');
    if (raw) {
      localStorage.removeItem('jobsState');
      const state = JSON.parse(raw);
      this.titleQ  = state.titleQ ?? '';
      this.skillQ  = state.skillQ ?? '';
      this.filters = state.filters ?? this.filters;
    }

    this.searchSub = this.searchSubject.pipe(
      debounceTime(300)
    ).subscribe(() => {
      this.filters = { ...this.filters, page: 1 };
      this.loadSubject.next();
    });

    this.loadSub = this.loadSubject.pipe(
      switchMap(() => {
        this.loading.set(true);
        return this.jobsService.getJobs({
          ...this.filters,
          q: this.titleQ || undefined,
          skill: this.skillQ || undefined,
          isUs: this.locationFilter.usOnly() ? true : undefined
        });
      })
    ).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });

    this.loadSubject.next();
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
    this.loadSub.unsubscribe();
    this.locationSub.unsubscribe();
    localStorage.setItem('jobsState', JSON.stringify({
      titleQ: this.titleQ,
      skillQ: this.skillQ,
      filters: this.filters,
    }));
  }

  onSearchInput(): void { this.searchSubject.next(); }

  onPage(e: PageEvent): void {
    this.filters = { ...this.filters, page: e.pageIndex + 1, pageSize: e.pageSize };
    this.loadSubject.next();
  }

  timeAgo(dateStr: string): string {
    const diff = Date.now() - new Date(dateStr).getTime();
    const days = Math.floor(diff / 86_400_000);
    if (days === 0) return 'Today';
    if (days === 1) return 'Yesterday';
    if (days < 30) return `${days}d ago`;
    const months = Math.floor(days / 30);
    return months === 1 ? '1 month ago' : `${months} months ago`;
  }

}
