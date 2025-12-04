import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { GradingPageComponent } from './pages/grading-page/grading-page.component';

const routes: Routes = [
    { path: '', component: GradingPageComponent }
];

@NgModule({
    imports: [RouterModule.forChild(routes)],
    exports: [RouterModule]
})
export class GradingRoutingModule { }
