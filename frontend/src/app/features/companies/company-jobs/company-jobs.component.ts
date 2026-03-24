import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Job } from '../../../core/models/job.model';
import { CompaniesService } from '../../../core/services/companies.service';

@Component({
  selector: 'app-company-jobs',
  standalone: true,
  imports: [NgIf, NgFor, RouterLink, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './company-jobs.component.html',
  styleUrl: './company-jobs.component.scss'
})
export class CompanyJobsComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private companiesService = inject(CompaniesService);

  companyId = 0;
  jobs = signal<Job[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    this.companyId = Number(this.route.snapshot.paramMap.get('id'));
    this.companiesService.getCompanyJobs(this.companyId).subscribe({
      next: j => { this.jobs.set(j); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }
}
