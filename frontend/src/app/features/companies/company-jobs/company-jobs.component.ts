import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgFor, NgIf } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { skip, switchMap } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Job } from '../../../core/models/job.model';
import { CompaniesService } from '../../../core/services/companies.service';
import { LocationFilterService } from '../../../core/services/location-filter.service';

@Component({
  selector: 'app-company-jobs',
  standalone: true,
  imports: [NgIf, NgFor, RouterLink, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './company-jobs.component.html',
  styleUrl: './company-jobs.component.scss'
})
export class CompanyJobsComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private companiesService = inject(CompaniesService);
  private locationFilter = inject(LocationFilterService);

  companyId = 0;
  jobs = signal<Job[]>([]);
  loading = signal(true);

  private locationSub!: Subscription;

  constructor() {
    this.locationSub = toObservable(this.locationFilter.usOnly).pipe(skip(1)).subscribe(() => this.load());
  }

  ngOnInit(): void {
    this.companyId = Number(this.route.snapshot.paramMap.get('id'));
    this.load();
  }

  ngOnDestroy(): void {
    this.locationSub?.unsubscribe();
  }

  private load(): void {
    this.loading.set(true);
    this.companiesService.getCompanyJobs(this.companyId, this.locationFilter.usOnly() ? true : undefined).subscribe({
      next: j => { this.jobs.set(j); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }
}
