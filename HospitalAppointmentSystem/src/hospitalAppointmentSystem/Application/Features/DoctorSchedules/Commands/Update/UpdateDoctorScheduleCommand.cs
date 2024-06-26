using Application.Features.DoctorSchedules.Constants;
using Application.Features.DoctorSchedules.Rules;
using Application.Services.Repositories;
using AutoMapper;
using Domain.Entities;
using NArchitecture.Core.Application.Pipelines.Authorization;
using NArchitecture.Core.Application.Pipelines.Caching;
using NArchitecture.Core.Application.Pipelines.Logging;
using NArchitecture.Core.Application.Pipelines.Transaction;
using MediatR;
using static Application.Features.DoctorSchedules.Constants.DoctorSchedulesOperationClaims;
using Application.Features.Doctors.Constants;
using NArchitecture.Core.CrossCuttingConcerns.Exception.Types;

namespace Application.Features.DoctorSchedules.Commands.Update
{
    public class UpdateDoctorScheduleCommand : IRequest<UpdatedDoctorScheduleResponse>, ISecuredRequest, ILoggableRequest, ITransactionalRequest
    {
        public int Id { get; set; }
        public Guid DoctorID { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }

        public string[] Roles => new[] { Admin, Write, DoctorSchedulesOperationClaims.Update, DoctorsOperationClaims.Update };

        public bool BypassCache { get; }
        public string? CacheKey { get; }
        public string[]? CacheGroupKey => new[] { "GetDoctorSchedules" };

        public class UpdateDoctorScheduleCommandHandler : IRequestHandler<UpdateDoctorScheduleCommand, UpdatedDoctorScheduleResponse>
        {
            private readonly IMapper _mapper;
            private readonly IDoctorScheduleRepository _doctorScheduleRepository;
            private readonly DoctorScheduleBusinessRules _doctorScheduleBusinessRules;
            private readonly IAppointmentRepository _appointmentRepository;

            public UpdateDoctorScheduleCommandHandler(IMapper mapper, IDoctorScheduleRepository doctorScheduleRepository,
                                             DoctorScheduleBusinessRules doctorScheduleBusinessRules,IAppointmentRepository appointmentRepository)
            {
                _mapper = mapper;
                _doctorScheduleRepository = doctorScheduleRepository;
                _doctorScheduleBusinessRules = doctorScheduleBusinessRules;
                _appointmentRepository = appointmentRepository;
            }

            public async Task<UpdatedDoctorScheduleResponse> Handle(UpdateDoctorScheduleCommand request, CancellationToken cancellationToken)
            {
                // �lk olarak g�ncellemek istedi�imiz mevcut kayd� alal�m
                var existingSchedule = await _doctorScheduleRepository.GetAsync(ds => ds.Id == request.Id && ds.DeletedDate==null);
                if (existingSchedule == null)
                {
                    throw new BusinessException("Doktor takviminizde b�yle bir kay�t bulunmamaktad�r.");
                  
                }

                // G�ncellenmek istenen tarih ve doktor ID'si ile silinmi� bir kay�t var m� diye kontrol edelim
                var conflictingSchedule = await _doctorScheduleRepository.GetAsync(ds => ds.DoctorID == request.DoctorID && ds.Date == request.Date);

                var appointment=await _appointmentRepository.GetAsync(x=>x.DoctorID==request.DoctorID && x.Date==request.Date &&x.DeletedDate==null);
                if (appointment != null)
                {
                    throw new BusinessException("Bu tarihe ait hastalar taraf�nda al�nm�� randevular bulunmaktad�r.Tarihi g�ncelleyemezsiniz");
                }
                else
                {
                    if (conflictingSchedule != null && conflictingSchedule.Id != request.Id)
                    {
                        if (conflictingSchedule.DeletedDate == null)
                        {
                            // Silinmemi� bir kay�tta �ak��ma var, hata f�rlatal�m
                            throw new BusinessException("Bu doktorun belirtilen tarihteki program� zaten mevcut.");
                        }
                        else
                        {
                            // Silinmi� bir kay�tta �ak��ma var, bu kayd� g�ncelleyelim
                            conflictingSchedule.Date = request.Date;
                            conflictingSchedule.StartTime = request.StartTime;
                            conflictingSchedule.EndTime = request.EndTime;
                            conflictingSchedule.UpdatedDate = null;
                            conflictingSchedule.DeletedDate = null;
                            //_mapper.Map(request, conflictingSchedule);
                            //conflictingSchedule.DeletedDate = null; // DeletedDate'i null yap
                            await _doctorScheduleRepository.UpdateAsync(conflictingSchedule);

                            // �stek yap�lan kayd� silindi olarak i�aretleyelim
                            existingSchedule.DeletedDate = DateTime.UtcNow;
                            await _doctorScheduleRepository.UpdateAsync(existingSchedule);

                            // G�ncellenen veriyi response olarak d�nelim
                            UpdatedDoctorScheduleResponse updatedResponse = _mapper.Map<UpdatedDoctorScheduleResponse>(conflictingSchedule);
                            return updatedResponse;
                        }
                    }
                    else
                    {
                        // �ak��an bir kay�t yoksa mevcut kayd� g�ncelleyelim
                        _mapper.Map(request, existingSchedule);
                        await _doctorScheduleRepository.UpdateAsync(existingSchedule);
                        // G�ncellenen veriyi response olarak d�nelim
                        UpdatedDoctorScheduleResponse updatedResponse = _mapper.Map<UpdatedDoctorScheduleResponse>(existingSchedule);
                        return updatedResponse;
                    }
                }
            }
        }
    }
}
