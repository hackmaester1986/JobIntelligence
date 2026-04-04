import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, skip, switchMap } from 'rxjs/operators';
import { Job, JobFilters } from '../../../core/models/job.model';
import { JobsService, PagedResult } from '../../../core/services/jobs.service';
import { CompaniesService } from '../../../core/services/companies.service';
import { LocationFilterService } from '../../../core/services/location-filter.service';

@Component({
  selector: 'app-jobs-list',
  standalone: true,
  imports: [
    RouterLink, FormsModule, NgFor, NgIf, DecimalPipe,
    MatCardModule, MatInputModule, MatButtonModule,
    MatFormFieldModule, MatChipsModule, MatProgressSpinnerModule,
    MatPaginatorModule, MatCheckboxModule
  ],
  templateUrl: './jobs-list.component.html',
  styleUrl: './jobs-list.component.scss'
})
export class JobsListComponent implements OnInit, OnDestroy {
  private jobsService = inject(JobsService);
  private companiesService = inject(CompaniesService);
  private locationFilter = inject(LocationFilterService);
  private searchSubject = new Subject<void>();
  private loadSubject = new Subject<void>();
  private searchSub!: Subscription;
  private loadSub!: Subscription;
  private locationSub!: Subscription;

  result = signal<PagedResult<Job> | null>(null);
  loading = signal(true);
  industries = signal<string[]>([]);
  selectedIndustries = new Set<string>();

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
    this.searchSub = this.searchSubject.pipe(
      debounceTime(300)
    ).subscribe(() => {
      this.filters = { ...this.filters, page: 1 };
      this.loadSubject.next();
    });

    this.loadSub = this.loadSubject.pipe(
      switchMap(() => {
        this.loading.set(true);
        const industries = this.selectedIndustries.size > 0 ? [...this.selectedIndustries] : undefined;
        return this.jobsService.getJobs({
          ...this.filters,
          q: this.titleQ || undefined,
          skill: this.skillQ || undefined,
          industries,
          isUs: this.locationFilter.usOnly() ? true : undefined
        });
      })
    ).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });

    this.companiesService.getIndustries().subscribe(list => this.industries.set(list));
    this.loadSubject.next();
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
    this.loadSub.unsubscribe();
    this.locationSub.unsubscribe();
  }

  onSearchInput(): void { this.searchSubject.next(); }

  toggleIndustry(industry: string): void {
    if (this.selectedIndustries.has(industry)) {
      this.selectedIndustries.delete(industry);
    } else {
      this.selectedIndustries.add(industry);
    }
    this.filters = { ...this.filters, page: 1 };
    this.loadSubject.next();
  }

  isSelected(industry: string): boolean {
    return this.selectedIndustries.has(industry);
  }

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
