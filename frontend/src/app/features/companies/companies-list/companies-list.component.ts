import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Company } from '../../../core/models/company.model';
import { CompaniesService } from '../../../core/services/companies.service';
import { PagedResult } from '../../../core/services/jobs.service';

@Component({
  selector: 'app-companies-list',
  standalone: true,
  imports: [
    RouterLink, FormsModule, NgFor, NgIf, DecimalPipe,
    MatCardModule, MatInputModule, MatButtonModule,
    MatFormFieldModule, MatProgressSpinnerModule, MatPaginatorModule
  ],
  templateUrl: './companies-list.component.html',
  styleUrl: './companies-list.component.scss'
})
export class CompaniesListComponent implements OnInit {
  private companiesService = inject(CompaniesService);

  result = signal<PagedResult<Company> | null>(null);
  loading = signal(true);
  searchQ = '';
  page = 1;
  pageSize = 50;

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.companiesService.getCompanies(this.searchQ || undefined, this.page, this.pageSize).subscribe({
      next: r => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  search(): void {
    this.page = 1;
    this.load();
  }

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.pageSize = e.pageSize;
    this.load();
  }
}
