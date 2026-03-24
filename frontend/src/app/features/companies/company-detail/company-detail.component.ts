import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe, NgIf, PercentPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Company, SnapshotPoint } from '../../../core/models/company.model';
import { CompaniesService } from '../../../core/services/companies.service';
import { CompanyHiringChartComponent } from './company-hiring-chart/company-hiring-chart.component';

@Component({
  selector: 'app-company-detail',
  standalone: true,
  imports: [
    NgIf, RouterLink, DecimalPipe, PercentPipe, DatePipe,
    MatCardModule, MatButtonModule, MatButtonToggleModule, MatProgressSpinnerModule,
    CompanyHiringChartComponent
  ],
  templateUrl: './company-detail.component.html',
  styleUrl: './company-detail.component.scss'
})
export class CompanyDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private companiesService = inject(CompaniesService);

  company       = signal<Company | null>(null);
  loading       = signal(true);
  snapshots     = signal<SnapshotPoint[]>([]);
  chartsLoading = signal(true);
  range         = signal('1w');
  companyId     = 0;

  ngOnInit(): void {
    this.companyId = Number(this.route.snapshot.paramMap.get('id'));
    this.companiesService.getCompany(this.companyId).subscribe({
      next: c => { this.company.set(c); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
    this.loadSnapshots('1w');
  }

  loadSnapshots(range: string): void {
    this.chartsLoading.set(true);
    this.companiesService.getSnapshots(this.companyId, range).subscribe({
      next: s => { this.snapshots.set(s); this.chartsLoading.set(false); },
      error: () => this.chartsLoading.set(false)
    });
  }

  onRangeChange(r: string): void {
    this.range.set(r);
    this.loadSnapshots(r);
  }
}
