import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Company } from '../../../core/models/company.model';
import { Job } from '../../../core/models/job.model';
import { CompaniesService } from '../../../core/services/companies.service';

@Component({
  selector: 'app-company-detail',
  standalone: true,
  imports: [NgIf, NgFor, RouterLink, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './company-detail.component.html',
  styleUrl: './company-detail.component.scss'
})
export class CompanyDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private companiesService = inject(CompaniesService);

  company = signal<Company | null>(null);
  jobs = signal<Job[]>([]);
  loading = signal(true);

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.companiesService.getCompany(id).subscribe({
      next: c => { this.company.set(c); this.loading.set(false); }
    });
    this.companiesService.getCompanyJobs(id).subscribe({
      next: j => this.jobs.set(j)
    });
  }
}
