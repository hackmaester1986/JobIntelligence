import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Job, JobFilters } from '../../../core/models/job.model';
import { JobsService, PagedResult } from '../../../core/services/jobs.service';

@Component({
  selector: 'app-jobs-list',
  standalone: true,
  imports: [
    RouterLink, FormsModule, NgFor, NgIf,
    MatCardModule, MatInputModule, MatButtonModule,
    MatFormFieldModule, MatChipsModule, MatProgressSpinnerModule, MatPaginatorModule
  ],
  templateUrl: './jobs-list.component.html',
  styleUrl: './jobs-list.component.scss'
})
export class JobsListComponent implements OnInit {
  private jobsService = inject(JobsService);

  result = signal<PagedResult<Job> | null>(null);
  loading = signal(true);

  filters: JobFilters = { page: 1, pageSize: 20 };
  searchQ = '';

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.jobsService.getJobs(this.filters).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  search(): void {
    this.filters = { ...this.filters, q: this.searchQ || undefined, page: 1 };
    this.load();
  }

  onPage(e: PageEvent): void {
    this.filters = { ...this.filters, page: e.pageIndex + 1, pageSize: e.pageSize };
    this.load();
  }
}
